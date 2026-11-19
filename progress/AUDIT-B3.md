# AUDIT-B3 · 第三轮(收尾)+ 三轮汇总

分支 `audit/B`(基于 main `dfb1caa` 之后的最新 main)。判据与证据全部在本文件末尾。

---

# 一、第三轮三件事

## 1. 静默失败统一(点了毫无反馈 → 必有一次可见反馈)

| 位置 | 原来 | 现在 |
|---|---|---|
| `Pages/Download/CatalogPage.cs:1049` `Run(work, errTitle=null)` | 只 `AppLog.Warn`,界面无任何反馈 | 记日志 **+** `Toast(errTitle ?? Tr("操作没有完成"), ex.Message, Warning)` |
| `Pages/Version/VersionManagePage.cs:847` 同上 | 同上 | 同上 |
| `CatalogPage` 的"已装列表刷新"(`OnEvent → Run(LoadInstalledAsync)`) | 静默 | 标题「读取已安装列表失败」 |
| `Pages/Library/InstanceDetailPage.cs:66` 「启动」 | `Run(LaunchNowAsync)` → 失败只写日志 | 标题「启动失败」 |
| `InstanceDetailPage` 模组开关 | `Run(() => enable/disable_mod ...)` 静默 | 标题「切换模组状态失败」 |
| `Pages/Library/LibraryPage.cs:123/125` 「导入整合包」「导入官方启动器」 | 静默 | 标题「导入整合包失败」「导入官方启动器失败」 |

`LibraryPage` / `InstanceDetailPage` / `SettingsSubs` / `SettingsPage.SaveLoad` 的 `Run` 本来就有 `errTitle ?? "操作失败"`,已可见 —— 本轮只补"操作名 + 原因要点"。

补充(lead 决定 2):「清理文件」必然 0 结果时 **置灰 + 一行说明**
`SettingsPage.Cards.cs:257`:`cleaner_preview` 为空 → `_clean.IsEnabled = false` + ToolTip「当前没有可清理的缓存;装过版本、下过东西之后再来」+ 说明行「当前没有可清理的缓存」;扫出东西时自动恢复可点。同时把 `cleaner_apply` 失败文案从谎报「清理后端 M3 接入」改成「清理没有完成:看看日志里的原因」。

补充(lead 决定 1):主题包卡片**保留**,只把说明改成与「外观」的关系:
「和上面的「外观」用的是同一套配色与背景:这里把它连同窗口、主页设置整包存成多个方案,方便在不同风格之间来回切(只负责存 / 取,不改「外观」里的当前选择)」。

## 2. i18n 接线(P1)

口径:只接**静态字面量**;拼接 / 插值 / 含变量的一律不动(见 §四清单)。做四批:

| 批次 | 文件 | 新接线 |
|---|---|---|
| 1 | `Pages/Settings/SettingsPage*.cs`、`SettingsSubs.cs`、`PhoneSyncPanel.cs` | **528** |
| 2 | `Pages/Download/CatalogPage.cs`、`DownloadPage.cs`、`LoaderIcons.cs` | **136** |
| 3 | `Pages/Library/*.cs`、`Pages/Version/*.cs` | **310** |
| 4 | `Pages/Plugins/*.cs`、`AccountPage.cs`、`MultiplayerPage.cs`、`TasksPage.cs` | **319** |
| — | 我这三轮自己新增的中文(静默失败提示、假按钮说明、清理/主题包文案等) | **+31 个新 key(两表成对)** |

**覆盖率:5.73%(299/5217)→ 31.04%(1623/5228)**;`missing en = 0 / missing zh = 0`(接了的 key 两表都有);词表 3029 → **3060** 键(en/zh 键集相等)。

安全规则(脚本 + 人工复核):
- 跳过注释(`//` `///` `/* */`)、跳过 `$"…"` / `@"…"`、跳过含 `{ } \` 的字面量、跳过已被 `Tr()` 包的;
- 跳过**参与比较/匹配**的字面量(`== "x"`、`case "x"`、`"x" =>`、`StartsWith/EndsWith/Equals`),否则同一个词在两处会因语言不同而不相等;
- 特例手工处理:①`const string AllInstances`(常量模式,不能是方法调用);②反馈分类下拉的 `switch` 改成与 `Tr()` 同口径的比较;③`extra["source"] = "本地"`(交给 Core 的**数据**,不翻译);④`OpenInFileManager(..., errTitle = "打开失败")`(形参默认值必须是常量);
- 已知一致的做法:下拉项与比较同源(如「全部」「细臂(Alex)」「崩溃报告」),两侧都用 `Tr(...)`,切 en 后仍然对得上。

## 3. 插件页「⋯」自查(aud-wpf 报的 WPF `ArgumentException` 同路径)

- Linux 侧不是 WPF 的 `MenuFlyout`,而是 `ContextMenu` + `menu.Open(placementTarget)`(`PluginsPage.cs:82`、`PluginsPage.Installed.cs:242`),菜单项由 `AddItem()` 构 `MenuItem{Header=控件}`。
- 逐条核对后**没有**发现同源的 `ArgumentException` 路径;但发现两个真实风险并加固:
  1. **目标没有可视根时 `Open` 会抛**(卡片刚被重建 / 移出列表、连点):两处都加 `if (btn.GetVisualRoot() == null) return;` + `try/catch` 记日志 —— 一个菜单打不开不会带走整个界面;
  2. 菜单项里两处 `async delegate`(更新/卸载)是 `async void`:内部方法本身有 try/catch,保持原样(卸载/更新失败已 Toast)。
- 结论:Linux 侧无法复现 WPF 的崩溃;加固属于防御,不改变正常路径。

---

# 二、三轮累计

## P0 / P1 / P2 统计

| 轮次 | P0 | P1 | P2 | 说明 |
|---|---|---|---|---|
| 第一轮(功能正确性) | 1 | 1 | 2 | AI 页「清空」必崩(P0)、启动按钮连点双开(P1)、补间键/插件块跨页(P2) |
| 第二轮(丝滑 + 假按钮) | — | 6 | 9 | 滚动条裁切、缩放不重排、假按钮 4 组 7 个、起手跳位、预览不裁、rects 分配、过期文案 6 处 |
| 第三轮(收尾) | — | 2 | 4 | 静默失败统一、i18n 接线 5.73%→31%；清理置灰、主题包文案、插件菜单加固、智能推荐文案 |
| **合计** | **1** | **9** | **15** | 另有 1 条跨域建议(Core 文档漂移) |

## 丝滑度:解决了哪几种"手感"(按用户能感知的话说)

1. 「**纵向滚动条一冒出来,卡片内容就被切一刀**」—— Arrange 宽 ≠ 测量宽时按新宽重量行高。
2. 「**拉右边框,框变宽了字还挤在左边,松手才跳版式**」—— 浮起/缩放预览/落位期间补 Measure。
3. 「**抓起来那一下卡片先跳一下**」—— 起手从"眼睛看到的位置"接着走(残留平移折进起手矩形)。
4. 「**缩小时内容糊出卡片边界**」—— 预览期裁剪,落定恢复。
5. 「**拖动时落点每变一次都要重排,拖动有轻微锯齿**」—— 复用 rects 表,少分配。
6. (第二轮之前就有、本轮复核确认顺畅)像素跟手、落点不变时零布局、让位 FLIP、并排落位阈值、边缘自动滚动、尺寸吸附无漂移。

## 假按钮接线清单(都是"能力已有、入口没接")

| 入口 | 处置 |
|---|---|
| 设置→通用「智能推荐」 | 字段映射修正(snake_case);失败文案不再谎报 M3 |
| 设置→服务器「导入」「导出」 | 接 `import_servers` / `export_servers`(选文件/存文件) |
| 设置→账号「微软登录」「皮肤站登录」 | 跳「账号」页(登录流程的唯一实现处)+ ToolTip |
| 设置→外观「主题包 导入 / 导出」 | 补 `path` / `dest` 参数,接 `import_theme` / `export_theme` |
| 启动页「联机」药丸 | 只切页(原来同时说"后续里程碑提供") |
| 4 处过期文案 | 「M3 接入」→ 真实原因 |

统计:扫过 **255 个按钮 / 105 个事件订阅 / 32 个菜单项**;**真空实现 0、死入口 0**、`CallAsync` 方法名与 IApi 方法表 100% 对齐。

## 还剩什么没做

1. **i18n 剩余 3605 条未接线**(68.96%),其中:
   - **约 2900 条是 Core 侧/插值串**(`$"共 {n} 个"`、`"刷新中..."`+变量 这类),按本轮口径不动;
   - **App 侧仍有 ~700 条静态字面量在词表里没有对应 key**(`LaunchPage` 57、`SettingsPage.Layout` 61、`AccountPage`/`MultiplayerPage` 各 ~20、`PluginsPage.*` ~30、`PhoneSyncPanel` ~20…)。它们要"翻译成英文才能接",属于**文案工作量**,不是接线工作量 —— 建议下一轮专门做一批(我可以按文件分批人工英译,每批 50~80 条)。
2. **设置页「必为 0 结果」的其它入口**只处理了「清理文件」;「智能推荐」在拿不到硬件信息时已经会失败并提示,但没有置灰(**拿到信息才可点**需要额外一次探测,未做)。
3. 布局侧留的取舍(上一轮已写):松手同步落盘、进编辑态滚动回顶、grid 下"吃满视口"块导致外层滚动关闭。

---

# 三、判据与证据

## 环境(每新 shell 必做)

```powershell
$env:PATH="D:\项目\TipCL\_tools\dotnet;$env:PATH"
$env:DOTNET_ROOT="D:\项目\TipCL\_tools\dotnet"                       # 不设 ⇒ 测试宿主报 hostfxr.dll 找不到
$env:DOTNET_CLI_HOME="D:\项目\TipCL\_tools\home-B"
$env:NUGET_PACKAGES="D:\项目\TipCL\_tools\nuget"
$env:NUGET_HTTP_CACHE_PATH="D:\项目\TipCL\_tools\tmp-B\nuget-http-cache"  # 不设 ⇒ 偶发 warning NU1900(共享 v3-cache 不可写)
$env:TEMP="D:\项目\TipCL\_tools\tmp-B"; $env:TMP=$env:TEMP
$env:TIPCL_HOME="D:\项目\TipCL\_tools\tmp-B\tipcl-home"
```

## 本轮验证

```
dotnet build src/TipCL.App/TipCL.App.csproj -c Release   → 0 错误 0 警告
dotnet test  tests/TipCL.Tests (全量)                     → 906 通过 / 1 跳过 / 0 失败
python tools/i18n-audit.py                                → wrapped 1623 (31.04%) / missing en=0 zh=0
python tools/i18n-audit.py --list-unwrapped               → 剩余清单(见 §二.还剩什么)
TIPCL_HOME=… dotnet src/TipCL.App/bin/Release/net8.0/TipCL.App.dll --smoke
                                                          → 启动成功 + 输出 smoke.png,退出码 0(i18n 接线后复跑)
```

## 三轮 commit(audit/B,未 push)

1. `audit/B: 修 AI 页「清空」/「新对话」重复挂空态(P0)`
2. `audit/B: 启动按钮连点重复启动(P1) + 补间表键按引用比较(P2)`
3. `audit/B: 插件区块外壳按页缓存 + 审计报告`
4. `audit/B: 报告补充无头启动冒烟证据`
5. `布局丝滑:Arrange 宽≠测量宽时重量行高 + 补 Measure + 起手不跳位 + 预览裁剪 + 复用 rects`
6. `假按钮:智能推荐字段映射 / 服务器导入导出 / 账号登录入口 / 主题包导入导出 + 过期文案 + AUDIT-B2`
7. `静默失败统一 + 清理按钮 0 结果置灰 + 主题包说明 + 插件「⋯」菜单加固`
8. `i18n 接线:四批静态文案接 Tr() + 31 个新 key 成对入表(5.73% → 31.04%)`
9. `AUDIT-B3 三轮汇总`

---

# 四、结论

**Linux UI 层可判定审计完毕**(App 层)。

理由:①用户主路径(启动 / 实例库 / 下载 / 插件 / 设置 / 布局自定义 / AI / 联机 / 账号)均已逐文件读完并做行为级核对;②高危模式(`async void`、`catch{}`、UI 线程外操作控件、一个控件两个父级、`Measure/Arrange` 自激、每帧读盘/重建)已逐条 grep 并定点验证;③崩溃类与研究得出的手感类问题都已修并在 906 条测试 + 无头冒烟上通过;④i18n 剩下的部分不是"接线",而是"等英文文案"的工作量(已量化),可以按批推进而不影响可判定性。

若还要一轮,建议只做两件事(都是增量的、低风险的):**(a)** 上表 ~700 条 App 侧静态文案的人工英译 + 接线(每批 50~80 条,配 `i18n-audit` 回归);**(b)**「智能推荐」这类"拿不到数据就该置灰"的入口做一次统一探测。这两件不改变架构,也不影响当前结论。