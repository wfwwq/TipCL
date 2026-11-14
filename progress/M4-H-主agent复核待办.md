# M4-H 主 agent 复核待办(交接用)

> 由主 agent(lead)在验收 M4-H 各工作包时写入。下一棒接手请**先读本文件**,再读 `PROGRESS.md` 的 M4-H 各小节。
> 本文件只记录「复核发现的问题」与「尚未完成项」,完成后请把对应条目划掉并在 PROGRESS 注明。

## A. 必须修:CLI 未拒绝不存在的实例(P3.5 遗留缺陷)

**状态:✅ 已修(第三棒,2026-11-12)。** 修法与验证:只在 CLI 入口加前置校验(Core 侧新增 `InstancesApi.FindInstance`,只查不改;`EnsureInstance` 语义未动),未知实例 → `exit=4` + 中文原因「没有这个实例:<名字>」;测试直接断言 exit=4 与输出原因。
主 agent 用重定向方式复测确认:`-i bogus_inst ...` → 4「没有这个实例:bogus_inst」;`-i` 单独 → 4「缺少 <实例>」;真实实例 → 3(启动失败:无离线账号)。

> 附:主 agent 早先报的「`-i` 单独出现返回裸 -1」是**主 agent 自己验证方法的失误**——用 `| Select-Object -First N` 截断管道会提前杀掉进程、把退出码伪造成 -1。测退出码要用重定向(`*> file`)而不是截断管道。第三棒的异议正确。

**以下是原始问题记录(留档)。** 移植代理汇报「未知实例 exit=3」,主 agent 进程外复验后确认**该结论误判了原因**。

复验命令与结果(用已构建的 Release 产物,非构建):

```powershell
# TIPCL_HOME 里有 .minecraft/versions/26.3 时:
dotnet src\TipCL.App\bin\Release\net8.0\TipCL.App.dll -i bogus_inst launch 26.3
# → [预检:warn] 客户端 jar 未直接找到: ...\_cmp\.minecraft\versions\26.3\26.3.jar
# → 实例: .minecraft | 版本: 26.3
# → 实例 Java 设置: C:/java/jdk-17/bin/javaw.exe
# → 启动游戏前请先创建一个离线用户(「账号」页 → 添加离线账号) → exit=3
```

与 `-i .minecraft launch 26.3` 的输出**逐行相同**。因此:

1. `-i <不存在的实例>` **没有被拒绝**,而是静默按默认实例继续;
2. 汇报里的 `exit=3` 来自 `CliExit.LaunchFailed`(没有离线账号),**不是**「实例不存在」;
3. 根因:`InstancesApi.EnsureInstance`(`src/TipCL.App/Common/Launch/LaunchCore.cs:341`)对未知名字是「目录不存在就 `Create()`」;而 `CommandLine.cs` 里 `CliExit.Usage = 4` 的注释自己写着「缺参数、**实例不存在**」,说明本意要做但没落地。

**为什么必须修**:桌面快捷方式写的就是 `-i <实例> launch <版本>`。实例名打错或实例被删时,快捷方式会**静默启动另一个实例的游戏目录**,玩家会在错的目录里玩(存档/模组全不对)。

**修法要求**:

1. 只在 **CLI 入口**做实例存在性前置校验:名字非空且确实不存在时,以 `CliExit.Usage`(4)退出,打印明确中文原因(如「没有这个实例:<名字>」)。**不要改 UI 路径上 `EnsureInstance` 的「不存在就创建」语义**(安装/导入流程依赖它)。
2. 测试**直接断言**「未知实例 → exit=4 且输出含该中文原因」。不要再用「没有离线账号」这类别的失败间接得到非零码——那正是本次误判的来源。
3. `-i` 单独出现(缺值)现在返回裸 `-1`,`CliExit` 里没有对应常量:归到 `Usage=4`,或显式加常量并说明。
4. 把 `PROGRESS.md` 的 P3.5 小节里「未知实例 exit=3」这句改成实测的真实码与原因。

## B. 文档纠正(docs/PARITY.md 已确认过期)

- **§3 第 115 行「缺失 Core」清单**:`Modpack` / `ModUpdate` 已于 M4-H P1 移植,应从缺失清单移入「已移植 Core」。`Shortcut` 在 P3.5 完成后同样移走(在此之前留在缺失里是**正确**的,仓库里确实只有 `ShortcutError.cs`)。
- **§4 第 131 行**:仍写着「剩 JavaPage、Playtime、Servers 独立页、Feedback 独立页、FirstRunWizard、CrashUi」,其中 **JavaPage(P3)、Feedback 独立页与 FirstRunWizard 与 CrashUi(P4)都已完成**,需更新。
- **§4 第 133 / 135 行**:「关键缺口汇总」条目需按实际完成情况勾掉或改写。
- **§2 第 100 行**:`AiFixWindow` 仍是「无」,做完后改。
- **新增固定小节**:「对照中发现的 Windows 参考实现缺陷」汇总,至少包含:
  - `ExportMany` 返回 `{dir,exported[],failed[]}` 而页面按 POCO 取 → 「全部导出」静默无反应(M4-H P1);
  - 存档/备份对话框按 POCO 取 `Size/LastPlayed/Mode`,Core 实际返回 `{name,path,icon,bytes,mtime}` → 那几列一直为空(M4-H P2);
  - 桌面快捷方式的 `-i ... launch ...` 参数**全工程无人消费**(`App.xaml.cs` 只认自检开关,全仓库 `"-i"` 只有 `Shortcut.cs` 一处)→ Windows 上该快捷方式本身就是坏的(M4-H P3.5);
  - `Shortcut.Chmod755` 是空方法 → `.desktop` 没有可执行位;Path/Icon/Exec 直接沿用 Windows 反斜杠路径,Linux 上不可用(M4-H P3.5)。

## C. 尚未完成的功能(按优先级)

1. **P5 剩余(AI 子系统)**:工具调用链 `ToolLine / ToolState / StepGroup` + Core `Launcher/Ai` 的 `Agent / Tools / ToolCall`;权限确认与选择卡 `ConfirmCard / AskBlock / AskCard / AiPermissionPanel`;`PlanCard / TaskCard`;`AiSelfTest / AiUiText`;会话记忆面板;图片输入(`AiPage.Images.cs`);`AiIcons`。
   - **`AiFixWindow.cs`(810 行)必须做**:`CrashUi` 的「交给 AI 修复」按钮因它而暂缺,做完后接上。
   - 遗留两条 Avalonia 限制(`Span` 不是控件 → 链接不可点;`Run` 没有 Background → 行内码无底色):能做就修,不能做保持如实标注。
2. **P6 插件宿主全链(完全未动)**:`PluginsPage.Market/Installed/Settings/Dev` 四分段拆分;`PluginActionBar / PluginOverlays / PluginDashCards / PluginEvents / PluginUi / AiViewStrip / PluginHostPage / MainWindow.Plugins`;Core `PluginManager / PluginHostApi / PluginSessions / PluginTools / PluginLlm / PluginAgentBridge / PluginHooks / LaunchHooks / DotNetPluginContext / JsonRpcPeer / NodeHostProcess / NodeRuntime / SecretStore / Sdk/PluginSdk / Sdk/PluginUiRegistry`。
   - **不引入 Node 宿主所需的第三方 NuGet 包**;没有宿主能力的部分按 P7 的做法逐项判定 + 写清理由,不做假按钮。
3. **P7**:统一拖放入口(`Services/FileDrop / FileKinds`)。
4. **P8**:插件动态顶部页签(`group:"top"`)。

## D. 复核时确认已 OK 的部分(不必重做)

- M4-H P1 CatalogPage 六分段、P2 版本管理页 + 三个对话框、P3 Java 子页、P4 反馈页/首启向导/CrashUi、P5 的 Markdown 链 + 对话页:文件、文档小节与验收数字与汇报一致(抽查过)。
- P7 的 Services 逐项判定(含 `WebViewHost` 871 行判不移植的理由)与 `DashHost` 死代码判定:有依据,保留。
- `TipCL.App.csproj` 的 `<InternalsVisibleTo Include="TipCL.Tests" />`:主 agent 已认可(不加依赖、不改行为),不要在此基础上再加别的依赖。
- 真实用户数据全程未动:`%LOCALAPPDATA%\TipCL\config.json` 保持 `2026-11-08 17:13:58.504`,`ai_chats.json` 保持 `2026-11-08 17:12:29.174`。

## E. 环境陷阱(会影响验收,务必遵守)

- 仓库目录带低完整性标签 → 仓库里编出的 `testhost.exe` / `TipCL.App.exe` 写不了 `%TEMP%`,直接 `dotnet test` 会几乎全红。**必须**:
  ```powershell
  cd D:\项目\tipcl\linux
  dotnet test tests/TipCL.Tests/TipCL.Tests.csproj --nologo -- RunConfiguration.DotNetHostPath=C:\Users\Administrator\.dotnet\dotnet.exe
  ```
- 冒烟:`TIPCL_HOME=D:\项目\tipcl\linux\_cmp`,可选 `SMOKE_PAGE` / `SMOKE_DARK` / `SMOKE_TASKS` / `SMOKE_SETTINGS_TAB` / `SMOKE_WAIT`,`smoke.png` 落在当前工作目录;退出码 0 且出图即成功。日志看 `_cmp\logs\tipcl-*.log` 的 WARN/ERROR。
- 用 `dotnet` 跑 CLI 时要接 **.dll**(`dotnet ...\TipCL.App.dll -i ... launch ...`),直接接 apphost `.exe` 会报 deps.json 扩展名不匹配。
- build 前 `dotnet build-server shutdown`;`PROGRESS.md` 是 CRLF、`DECISIONS.md` 是 LF,追加时保持风格。

---

## F. 主 agent 验收记录与剩余清单(2026-11-12 13:20)

### 已由主 agent 独立复跑通过的验收(不采信自证)

| 项 | 主 agent 实测 |
|---|---|
| Debug 全新重编(`--no-incremental`) | exit=0,**0 警告 0 错误** |
| Release 全新重编 | exit=0,**0 警告 0 错误** |
| `dotnet test`(带 DotNetHostPath)连续 3 次 | **232 通过 / 1 跳过 / 0 失败**(三次一致) |
| 全页冒烟矩阵 10 页 × 明暗 = 20 次会话 | **20/20 exit=0 且出图**;`_cmp/logs` 40 行、WARN/ERROR **0** |
| `--ai-selftest` | exit=0(32 项自检) |
| `--print-command` | exit=0,打印「实例 / 版本 / 启动参数」 |
| `-i bogus_inst launch 26.3` | **exit=4** +「没有这个实例:bogus_inst」(P0 确已修好) |
| `-i .minecraft launch`(缺版本) | exit=4 +「缺少版本:请用 -i <实例> launch <版本>」 |
| 真实用户数据 | 7 个 json 全部 mtime 未变(config 仍 2026-11-08 17:13:58.504) |
| 仓库卫生 | 无 `Zz*` 探针、仓库根无 `.desktop` 残留、真实桌面无 `.desktop`;仅有的 `dist/win-x64/smoke.png`(11-09)与 `tools/AppImage/tipcl.desktop`(11-08,已跟踪)均为旧产物 |
| git | 136 个改动未提交(遵守不 commit) |

视觉抽查:`L-plugins.png` 两分段 + 「Linux 版没有插件运行时」如实说明,文案把原因讲清;`D-ai.png` 头部(内置网关 / 权限档)、空态建议、输入区均正常。

`docs/PARITY.md` 第 85 行 `PhoneSyncPanel.cs` 标 ❌ 是**准确**的:Linux 只有「连接卡」(`SettingsPage.Cards.cs:88` 的 `BuildPhoneSyncCard`,IP+配对码 / USB),而 Windows `PhoneSyncPanel.cs` 是**世界同步与对比面板**(选本地与手机位置、逐项或一键同步),确实未做。

### 剩余需做的(真实缺口,非「已判定不移植」)

1. ~~**AI 服务层残留**(PARITY §2 AI 小节自己列出的「仍缺」):检查点回滚、上下文压缩、令牌与用量统计、MCP 插件工具、子代理、产物回读;以及 `AiChatOps` 斜杠命令、会话记忆面板、上下文占用环。~~
   **✅ 已完成(第四棒 R1 + R2,2026-11-12)**:Core 新增 `Checkpoint / Rewind / Store / Compact(+配置与结果类型) / Tokens / TokenState / Usage / Mcp / McpClient / McpError / Artifacts / AiFs / Trace / StopReason`,工具表补 `read_artifact` 与**只读** `dispatch_subagent`,写操作前按 `Tools.AffectedPaths` 建检查点;界面侧斜杠命令(7 条)、记忆面板、占用环、对话持久化、`/撤回` 真接 `Rewind`。判定与理由见 `DECISIONS.md` D-28 / D-29,验收数字见 `PROGRESS.md` M4-H R1~R4 小节。**`McpServer`(对外暴露)判不移植**(依赖 `BackendApi`,且外部客户端没有用户点确认)。
2. ~~**`PhoneSyncPanel`**(世界同步/对比面板)。~~
   **✅ 已完成(第四棒 R3)**:`App/Pages/Settings/PhoneSyncPanel.cs`(对比 + 逐项传 + 一键同步 + 日志),设置页手机同步卡补「同步面板」入口;`docs/PARITY.md` 第 85 行已由 ❌ 改 ✅。
3. ~~小尾巴:已安装页「有更新」徽标、市场详情截图(现只列链接)。~~
   **✅ 已完成(第四棒 R4)**:已安装卡片显示「有更新 → vX」(读市场索引 `updatable` + 版本号,菜单项变「更新到 vX」);市场详情用 `Common/Thumbs` 真的显示截图缩略图,加载失败给「点击用浏览器打开」占位。

> 第四棒(2026-11-12)复跑验收:**Debug / Release 全量重编 0 警告 0 错误**、`dotnet test` **299 通过 / 1 跳过 / 0 失败**(起点 232,新增 67 项)、`--ai-selftest` **50 项全过 exit=0**、全页冒烟矩阵 **10 页 × 明暗 = 20 次会话全部 exit=0 出图且 WARN/ERROR 0 行**、真实用户数据 mtime 未变。


### 明确阻塞、需用户或外部条件(不要硬做)

- **真模型端到端**:需要真实 API key,属对外调用,留给用户决定(AI 页与 `--ai-selftest` 都不联网;`--ai-selftest-send` 只打印「未跑」)。
- **真机验证**:Ubuntu 盒子(`mcp__box__*`)未接入,`.desktop` 双击、真机拖放、手机同步的对比/传输需盒子或连着手机时执行。

### 已判定为「不移植」(**不要再重新讨论**,理由见 PARITY §5/§7)

`SPanel / SmoothScroll / SideFrost / WebViewHost(871 行)/ Backdrop / ImeGuard`;插件宿主运行时半部(Node 宿主缺位;参考 .NET 契约在 net48 WPF 程序集里、UI 类型是 `System.Windows.FrameworkElement`,见 §7.2);`Overlays / ActionBar / DashCards / AiViewStrip`;`Wallpaper` 的 mp4 与目录轮播(无 MediaElement 等价宿主);`DashHost`(参考版死代码);`InstancePage.cs`(Windows 未注册进 NavDefs);`PlaytimePage / ServersPage` 独立页(Windows v2 也不是顶级页,已由设置页内层导航覆盖)。
