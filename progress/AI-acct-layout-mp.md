# AI-acct-layout-mp · 账号皮肤 + AI 排版 + 联机(Linux 主线)

- 分支 / worktree:`ai/acct-layout-mp` @ `D:\项目\TipCL\wt-ai-acct-layout-mp`,基于 `main@4277a02`
- 提交前缀:`AI-acct-layout-mp:`(未合并到 main,未 push)
- 只改了 Linux 一套(`src/`、`tests/`);`windos/` 只读参照,未改任何文件;未改版本号、未加 NuGet 依赖

## 1. 做了什么

### 1.1 先查清 Windows 版怎么接给 AI 的

| 能力 | Windows 版落点 | 结论 |
|---|---|---|
| 账号 / 皮肤 | `windos/src/TipCL/Core/Launcher/Ai/ExtraTools.cs`:`list_accounts` / `add_offline_account` / `switch_account` / `set_skin` | **同名移植** |
| 联机 | 同文件 `multiplayer`(`action = status / host / join / stop`,`room`),背后是 `BackendApi.terracotta_*` | **Windows 有专门工具** → 按任务书「Windows 有对应工具:同名移植」移植为 `multiplayer`,**不新增** `multiplayer_host / _join / _status / _stop` 四个名字(避免两边工具名分叉) |
| AI 排版 | `windos/src/TipCL/Core/Launcher/Ai/LayoutTools.cs`:`layout_get` / `layout_apply` | **同名移植**,并在 Linux 侧加上 D-30 门禁(见 1.3) |

Linux 背后用的 Core / 页面 API 全是现成的:账号走设置页「账号」(AccountPage / AccountGate / SkinPng)同一套 `IApi` 方法
`get_account_rows / add_offline_account / set_active_account / set_account_skin`;联机走联机页同一套
`terracotta_snapshot / terracotta_prepare / terracotta_host / terracotta_join / terracotta_idle`(`src/TipCL.Core/Launcher/Terracotta.cs`);
排版直接走 Core 的 `LayoutStoreV2`(`ui_layout_v2.json` 真源 + 单向写通 `ui_page_layouts.json`)。

### 1.2 新工具清单

| 工具 | 风险(side_effect / risk) | 只读 | 确认 | 预览 | 检查点 |
|---|---|---|---|---|---|
| `list_accounts` | read / low | ✅ | 否 | — | — |
| `add_offline_account` | write_local / low | ❌ | 是 | 账号名 + 「添加后会切换为当前账号」/「已有同名」 | 不做(见 §4-2) |
| `switch_account` | write_local / low | ❌ | 是 | 当前账号 → 目标账号 | 不做(同上) |
| `set_skin` | write_local / low | ❌ | 是 | 账号 / 皮肤文件 / 手臂模型 | 不做(同上) |
| `multiplayer` | **network** / medium | ❌ | 是 | 开房会拉起内核进程并连公共节点 / 加入的房间号 / 退出 | 不落盘 |
| `layout_get` | read / low | ✅ | 否 | — | — |
| `layout_apply` | write_local / medium | ❌ | 是 | 改动说明 + 摘要的统一 diff + 「改动前会备份为 AI 备份 <时间>」 | `ui_layout_v2.json` + `ui_page_layouts.json` |

- 风险分级照 Windows `ExtraTools.Meta`;**唯一差异**:`multiplayer` 的 side_effect 记 `network`(Windows 记 `write_local`)。
  它会拉起联机内核进程并连公共节点,按 `write_local` 记会被「文件类修改直接执行」(edit)档放行;记 `network` 后 edit 档也要确认,风险标签显示「联网」。
- 都不是删除类,确认卡保留「本对话都允许」(D-24 只禁删除/危险类)。
- 工具名、参数名、schema 描述与 Windows 逐字一致;`set_skin` 描述末尾补了「只对离线账号有效」(Linux 的 `set_account_skin` 对正版/外置会报错)。
  `set_skin` 另外兼容别名 `skin_file` / `skin_model`(任务书口径),schema 仍只写 Windows 的 `name / path / model`。

### 1.3 行为要点(比 Windows 版更严的地方都写在这里)

**账号**
- `list_accounts` 只返回白名单字段 `name / type / active / skin_model / skin_file`(与 Windows 投影同集合),`skin_file` 只给文件名;
  令牌、密码、uuid、外置 API 地址、头像 data URL 一律不出(测试同时断言字段集合与原值不出现)。
- `add_offline_account`:用户名照 Windows 正则 `^[A-Za-z0-9_]{3,16}$`;**新增保护**:`AccountManager.AddAccount` 按名字替换,
  同名的正版/外置账号会被离线号顶掉(令牌一起丢),所以先查一遍,同名就拒绝并说明。
- `switch_account`:找不到时列出现有账号名;大小写不同但唯一时按那一个切。
- `set_skin`:只认存在的 `.png`,只对离线账号;不写 `name` 时用当前账号。
  「对话里附带的图片」:AI 页发送带图消息时,把 **png 附件的本地路径**附在发给模型的文字末尾(`AcctLayoutMpTools.AttachmentNote`),
  模型才拿得到 `path`(Windows 版描述写了「可以是用户刚附的图片」,但它其实也没把路径交给模型)。
- `set_account_skin` 现在也广播 `ui_changed`(标题栏账号钮跟着变;此前只有增删/切换才广播)。

**联机(`multiplayer`)**
- `status`:`supported / installed / running / state / room / error / game_running`(Windows 同名字段 + `supported`)。
- `host` / `join`:不支持 → 直接说不支持;没装 → 起「准备陶瓦联机」后台任务(任务卡)并让用户装好后再试;
  **已装但没起**(Linux 联机页是「打开本页才起内核」,AI 入口没有这一步)→ 先起内核,最多等 15 s。
  `host` 后最多等 6 s 看房间号:有就直接返回房间号,没有就提示先「对局域网开放」再 `status`。
- `stop` = `terracotta_idle`(退出房间,内核保持运行;与 Windows 一致)。

**排版(`layout_get` / `layout_apply`)**
- `layout_get` 摘要与 Windows 同形(version / active_profile / profiles / shell / pages / widgets / splits)。
- `layout_apply` 支持 Windows 的全部组合:`page + blocks / mode / cols / gap`、`profile`、`json`、`widgets`、`shell_tabs`。
  执行顺序:**Plan(副本上改 + 门禁 + Sanitize)→ 把改之前的布局存成 `profiles["AI 备份 yyyy-MM-dd HH:mm:ss"]`(只留最近 5 份,同秒重名追加 `#2`)
  → `LayoutStoreV2.Save`(v2 + 写通 v1)→ `RefreshV1Cache`**。确认卡预览与执行共用同一份 Plan。
- **D-30 门禁**(Windows 版 `layout_apply` 没有,Linux 补上):对改后文档里所有 web / button 定义(当前布局 + 各方案 + 各响应式变体;
  `widgets` 参数与整份 `json` 两条路都过)——
  - 新写入或改了网址/动作的:`web` 只允许 https(禁 `file://` / `javascript:` / `data:` / `http` / 其它协议,须有主机名),
    `button` 只允许 `page:<已知页面>` / `launch:<实例>[|版本]` / `folder:data` / `rpc:<get_launcher_state|list_instances|plugins_list>`;
    **不合法整次拒绝、不落盘、不产生备份**;
  - **AI 不能替用户开启**:AI 写入/改过目标的 web / button 一律 `enabled=false`(D-30「必须由用户在设置里显式勾选 + 二次确认」;
    与 `4277a02`「导入危险件默认关闭」同口径),结果里提示用户去 设置 → 布局 → 自建区块 开启;用户早已开启且目标没变的保持原样。
  - 判定逻辑与 App 侧 `Pages/Layout/Widgets/WidgetCommon.TryAcceptWebUrl / TryParseAction` 逐字等价(Core 不能引用 App,
    只能复刻),**由测试 `Guard_AgreesWithWidgetCommon_OnEveryCase` 逐条对照锁死**(36 个输入、白名单常量相等)。
- 其它比 Windows 严的点:`mode` 非法直接报错(Windows 交给 Sanitize 静默改回 stack);方案数到 `LayoutSchema.MaxProfiles` 时拒绝
  (否则 Sanitize 会把刚加的 AI 备份悄悄丢掉);磁盘写失败时在结果里说明。
- **UI 即时刷新**:`Save` 触发 `LayoutStoreV2.LayoutChanged`,页面区块(`BlockHost`)本来就订阅 → 即时重排;
  标题栏页签不订阅这个静态事件(MainWindow 构造里写了原因),约定由调用方显式 `MainWindow.ApplyShellLayout()` ——
  AI 页在 `tool_done` 里对成功的 `layout_apply` 补这一步(`Pages/Ai/AiAcctLayoutMpHooks.AfterTool`)。
- **撤回一轮**:检查点会把 `ui_layout_v2.json` 还原,但 `LayoutStoreV2` 进程内缓存还是新的;`AiAcctLayoutMpHooks.AfterRewind`
  清缓存重读,内容确实变了才落一次盘(写通 v1 + 触发 LayoutChanged)并重排标题栏。

### 1.4 系统提示(AiData.cs)
在「# 改配置」与「# 说话方式」之间追加一个 4 行小节「# 账号 / 联机 / 界面布局」(账号先 list、不复述令牌;皮肤只对离线号且用附图路径;
开房用 `multiplayer host` 发房间号、没房间号先「对局域网开放」;布局先 get 再 apply、说清备份名、危险区块默认关)。
关键词表通过 `Tools.SelectToolSchemas` 里 `AiData.KEYWORD_TOOLS.Concat(AcctLayoutMpTools.Keywords)` 并入(与 Windows `Tools.cs:137` 同一写法),
三行关键词与 Windows `ExtraTools.Keywords` 同值。

## 2. 文件清单

新增(本包独占):
- `src/TipCL.Core/Launcher/Ai/AcctLayoutMpTools.cs` —— 7 个工具的 schema / 标签 / 关键词 / 账号字段白名单 / 附图路径附注 / 预览 / 检查点路径
- `src/TipCL.Core/Launcher/Ai/LayoutTools.cs` —— `layout_get` / `layout_apply`(Plan/Apply、AI 备份、D-30 门禁)
- `src/TipCL.Core/Launcher/Ai/ToolRunner.AcctLayoutMp.cs` —— `ToolRunner` 的 partial:7 个工具的执行体
- `src/TipCL.App/Pages/Ai/AiAcctLayoutMpHooks.cs` —— layout_apply 后重排标题栏、撤回后刷新布局缓存
- `tests/TipCL.Tests/App/AiAcctLayoutMpTests.cs` —— 32 例
- `progress/AI-acct-layout-mp.md` —— 本文件

修改(共享文件,均为一处追加式改动,行首注释 `AI-acct-layout-mp:`):
- `src/TipCL.Core/Launcher/Ai/Tools.cs`:`Schemas` 末尾 `AddRange`;`Label` 开头转发;`SelectToolSchemas` 关键词 `Concat`;`AffectedPaths` 开头转发
- `src/TipCL.Core/Launcher/Ai/AiToolTypes.cs`:`ToolMeta.ALL` 末尾追加 7 行
- `src/TipCL.Core/Launcher/Ai/ToolRunner.cs`:`sealed class` → `sealed partial class`;`ExecuteAsync` 的 `default:` 开头转发
- `src/TipCL.Core/Launcher/Ai/Preview.cs`:`ChangePreview` 开头转发
- `src/TipCL.Core/Launcher/Ai/AiData.cs`:系统提示追加一个小节
- `src/TipCL.Core/Launcher/Ai/AiSelfTest.cs`:`VerifyAsync` 末尾追加第 11 段(4 条纯逻辑检查)
- `src/TipCL.Core/Resources/locales/zh_CN.json` / `en.json`:末尾各追加 18 个确认卡预览用的键(两表键集合仍一致,3061 → 3079)
- `src/TipCL.App/Pages/Ai/AiPage.cs`:`tool_done` 后一行 hook、撤回成功后一行 hook、发图时附 png 路径(3 处)
- `src/TipCL.App/Common/Api.cs`:`set_account_skin` 保存后补广播 `ui_changed`(1 行)

## 3. 测试与实测

- 新增 xunit **32 例**(`AiAcctLayoutMp*` 四个类):工具表 5 / 账号 6(含 1 条真实 `InProcessApi` 全链路:加号 → 切换 → 换皮肤 → 列表,
  隔离 TIPCL_HOME、唯一名、收尾删号)/ 联机 4 / 排版 17(其中 web 5 种非法地址、button 6 种白名单外动作两组 Theory)。
  排版用例归入「TipCL 真机交互回归」集合(与其它碰 `LayoutStoreV2` 静态状态的用例一起串行),存储指到临时目录。
- `--ai-selftest` 追加 4 条:工具已进表、账号列表不带令牌、拒 file:// 网页块、拒白名单外按钮动作。
- 实测结果见 complete 汇报(Debug 构建 0 错误、无新增警告;全量 `dotnet test` 通过 1001 / 跳过 7 / 失败 0;`--ai-selftest` exit=0,通过 58)。

## 4. 遗留问题与风险

1. **`multiplayer status` 也要确认**:与 Windows 一样整条工具 `ro=false`,查状态也会弹确认卡(可点「本对话都允许」)。
   要拆成只读需要改工具名或在 `RunAsync` 里按参数分级,两边都会偏离 Windows,本包没做。
2. **账号类不做检查点**:`accounts.json` 里是封存过的令牌 / 钥匙串引用,按文件回退会冲掉之后新登录的账号或留下悬空引用(Windows 版同样不做)。
   回退靠界面操作(切回 / 删号);确认卡照常弹。
3. **联机真机未跑通**:本机测试只走假门面 + 契约;内核下载、打洞、房间号产生需要真网络和另一台机器,未在本包验证(`host` 的等待上限 15 s / 6 s 是经验值)。
4. 设置页「布局」视图如果正开着,AI 改完后方案列表 / JSON 卡片不会自动刷新(它们不订阅 LayoutChanged),切一下视图即可;分栏比例若由 `json` / `profile` 改动,
   已经打开的页面要重新进入才按新比例排。
5. D-30 判定是 `WidgetCommon` 的 Core 复刻:以后改白名单必须两处同改,否则对照用例会红(这是有意的锁)。

## 5. 合并时注意

- 共享文件的改动都是单点追加,和其它 4 个 AI 包大概率在同一位置相撞:`ToolMeta.ALL` 末尾、`Tools.Schemas` 的 `_schemas = cached` 前、
  `Tools.Label` 开头、`Tools.AffectedPaths` 开头、`ToolRunner.ExecuteAsync` 的 `default:`、`Preview.ChangePreview` 开头、`AiData.SYSTEM_PROMPT`「# 说话方式」前、
  `AiSelfTest.VerifyAsync` 的 `return result;` 前、两份 locales 的末尾 —— 解决冲突时**两边都保留**即可。
- `ToolRunner` 改成了 `partial`:其它包如果也改了这一行,保留一个 `partial` 即可。
- 如果别的包也注册了 `list_accounts` / `multiplayer` / `layout_*` 同名工具,`ToolMeta.Map` 的 `ToDictionary` 会因重复键在启动时抛异常 —— 合并后务必跑一次 `--ai-selftest`。
- locales 两表追加的键与其它包不重名(都是确认卡预览文案);合并后跑 `I18nCoreLocalesTests`(键集合必须相等)。

### 合并后要改的 README / PARITY 行(本包按约定未改)
- `README.md:95`:「**仍缺**的是 AI 排版工具(`layout_apply` 与「AI 备份 <时间>」方案,只在 `windos/` 里)」→ 改为已落地(`src/TipCL.Core/Launcher/Ai/LayoutTools.cs`),并注明 Linux 版对 web/button 多执行 D-30 门禁、AI 写入的危险块默认关闭。
- `README.md:57`「开个房和朋友联机 → 启动内置 Terracotta,把房间号发给你」:Linux 侧现在也成立(`multiplayer` 工具)。
- `docs/README.md:99`「Linux 侧除『AI 排版』外的入口都在」、`docs/README.md:118`「**尚未落地**:AI 排版工具 …… `src/` 里 `LayoutTools` 与字符串『AI 备份』零命中」→ 两处都改为已落地。
- `docs/PARITY.md`:AI 工具表一节补 7 个工具(账号 4 / 联机 1 / 排版 2)及「multiplayer 副作用记 network」「layout_apply 加 D-30 门禁 + AI 不能开启危险块」两条 Linux 差异。
