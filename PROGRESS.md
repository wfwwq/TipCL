# PROGRESS（工作包总表）

每个工作包的详细记录在 `progress/WP-X.md`（子代理各写各的，避免并发冲突）。主 agent 汇总到这里并负责 git 提交。

| 包 | 内容 | 主要文件（独占） | 状态 |
|---|---|---|---|
| WP-A | 窗口外壳：Win10 圆角(SetWindowRgn)、矢量窗口按钮、标题栏（5 页签含插件、AI 图标、头像、C 桥仅断开显示）、全局圆角/卡片样式 token | MainWindow.xaml(.cs)、Controls.xaml 及主题资源字典、Services/Backdrop.cs | 完成待验（未编译）。含 download 接线。资源键清单见 progress/WP-A.md。遗留：启动页背景铺到标题栏下；设置子页时“设置”页签不亮；Win10 Region 圆角锯齿/缩放闪烁需实测 |
| WP-B | 启动页 v2：左对齐、截图背景+渐变、去 HeroGrid/光晕/胶囊、底部新闻卡 | Pages/LaunchPage.cs | 完成待验（未编译）。详见 progress/WP-B.md；LaunchBackground.cs 已并入 LaunchPage.LoadShot。遗留：标题栏延伸到截图上→WP-A；B.HeroGrid/HeroGlow 键可删→WP-F；删抽屉时保留 multiplayer NavDef |
| WP-C | 下载页 v2 + 新安装页（加载器卡片、版本下拉、Fabric API、OptiFine、底部汇总）+ 加载器官方图标运行时下载缓存 | Pages/Download*、新建安装页文件、新建图标缓存服务 | 完成待验（未编译）。新建 DownloadPage/InstallPage/LoaderIcons，删 VersionPage 旧安装 UI、TasksPage HubRow。**依赖 WP-A 在 MainWindow 接 download NavDef（同版合入）** 及 WP-A 新键 Seg.Rail/Seg.Item/B.Card/B.CardBorder/B.RowLine/B.RowHover。详见 progress/WP-C.md |
| WP-D | 插件页卡片化（已安装/市场分段，⋯ 收开发与市场源）+ 设置页左导航重排 | Pages/Plugins/*、Pages/SettingsPage.cs | 完成待验（未编译）。插件设置卡移到插件页 ⋯；设置页嵌入子页为独立实例。遗留：设置里切账号后标题栏账号名不即时刷新（需 MainWindow）；插件卡固定 3 列。详见 progress/WP-D.md |
| WP-E | 实例详情页（分段 模组/存档/资源包/光影/设置，模组行开关），实例库“管理”进入 | 实例相关页面 | 完成待验（未编译）。新建 InstanceDetailPage，LibraryPage 管理内部切换，VersionManagePage 加 Folder 属性。体积估计 +4–6 KB 大概率超红线，需实测。详见 progress/WP-E.md |
| WP-G | **premium 版**（交接必读 §2.5）：csproj 加 Premium 开关；嵌入加载器官方图标/草方块/hero.jpg；启动页无截图时用 hero.jpg；真毛玻璃；去掉为体积做的删减 | src/TipCL/TipCL.csproj、LoaderIcons.cs、LaunchPage.cs（#if PREMIUM 分支） | 未开始（排在 WP-F 之后，下一轮开工即做） |
| WP-H | 双版本测试与交付：两版各自 build/test、GOAL §6 测试全跑、compact 体积检查、产出 TipCL-compact.exe / TipCL-premium.exe（需用户 Windows 环境） | — | 未开始 |
| WP-F | 收尾：删“更多”抽屉、统一圆角、全局一致性审查、DECISIONS 追加 | 全局 | **已验收**（2026-11-07，commit 6ebc26e）：5254d8f 删除审查通过、无悬空引用；\uec7a→\uebe8；SyncTabs 设置子页点亮；账号芯片即时刷新。0 错误、127/128、5,705,216 B。详见 progress/WP-F.md |
| WP-V | **页面布局自定义**（用户新需求 2026-11-07，插队；编号 WP-U 已被启动页贴图包占用故顺延）：大多数页面右键「自定义布局…」→ 区块拖拽重排 + 显隐 + 布局 JSON 导出/导入分享。新建框架 4 件（PageBlock/PageLayoutStore/BlockHost/PageLayoutIO，约 1100 行）+ 15 个单测；接入 11 页：设置(3 子页)/下载/任务/账号/Java/时长/服务器/联机/反馈 套 BlockHost，启动页英雄区固定+新闻/任务/时长/便签/日志块，实例库双栏 Store 直写实现文件夹栏显隐。默认布局逐像素等价于现状 | Pages/PageBlock.cs、PageLayoutStore.cs、BlockHost.cs、PageLayoutIO.cs、tests/PageLayoutTests.cs、11 个页面文件 | **已验收**（2026-11-07，三代理并行交付）：快照构建 0 错误 + PageLayout 测试 15/15；全量集成构建 0 错误、测试 143/144（唯一失败=基线 PhoneLinkTests.FullFlow）。用户明确本功能不受 compact 体积红线约束，实测 5,743,616 B。详见 progress/WP-V.md |

## 中断记录
- 2026-11-06 13:52（UTC-10）：日额度达到 80%，用户叫停。WP-A/B/C 三个子代理被取消，均**未写 progress/WP-X.md**。已落盘的半成品改动已提交为一个 commit（"WIP 中断"），未经验收、未编译。
- 接手顺序建议：先审 WP-A 主题改动（其它包依赖其资源键）→ 补完 WP-A → 审/补 WP-B → 重新做 WP-C → WP-D → WP-E → WP-F。
- 开工前及每派一批子代理后用 billing_status 查日额度；70% 警戒，80% 停工打包。

## 2026-11-06 第二轮（接手）
- 13:58（UTC-10）日额度 0%。仓库移到项目 repo/tipcl。并行派出 WP-A、WP-B（审补半成品）、WP-C（重做）。WP-D/E 待 WP-A 资源键落定后再派。

## 额度记录
- 2026-11-06T13:58-10:00 日额度 0%
- 2026-11-06T14:08-10:00 日额度 52%（三个并行子代理 ~10 分钟耗掉约一半；60% 后只派单个小任务）
- 2026-11-06T14:10-10:00 日额度 54%（WP-A 完成；接下来单派 WP-D）
- 2026-11-06T14:15-10:00 日额度 61%（WP-D 完成，耗约 7%；单派 WP-E）
- 2026-11-06T14:20-10:00 日额度 67%。用户追加双版本交付（交接必读 §2.5），登记为 WP-G/WP-H。
- 2026-11-06T14:23-10:00 日额度 74%，用户叫停。WP-F 子代理取消，半成品提交为 WIP，打包交接 TipCL-handoff-2026-11-06-02.zip。

## 下一轮接手顺序（2026-11-06 第二轮结束时）
1. 审 WP-F 半成品 → 补完 WP-F（含 DECISIONS D-11+）。
2. WP-G premium 版（交接必读 §2.5）。
3. 请用户在 Windows 上编译两版、跑测试、量体积；compact 若超红线，按 progress/WP-*.md 里各包给出的“超了先删什么”回收。
4. WP-H 双版本测试与交付。
已知整体风险：本轮全部未编译；compact 体积大概率超 5,707,264 B（WP-A/D/E 各自估计增加数 KB，WP-F 删除约 1000 行可能抵消）。

## 2026-11-07 第三轮（用户 Windows 机接手，环境：本机可编译）
- 环境：zip 解包到 `windos\tipcl`（git 完整）。dotnet SDK 10.0.403 可用——本轮起所有改动真编译验证。zip 内陈旧 obj 曾造成 68 个假 CS0103，净 obj 后 HEAD 编译通过。
- WP-F 审查+补完（**commit 6ebc26e**）：删除安全；字形修复；SyncTabs/账号芯片补完。compact 5,705,216 B、127/128。
- WP-G premium（**commit ceb7061**）：PREMIUM 开关 + PremiumAssets 6 件嵌入 + hero 回退 + WallHost 标题栏沉浸。compact 字节不变，premium 6,000,128 B。
- WP-H/WP-I 双版 --smoke 全绿（22 页/44 控件/19 弹窗/0 异常，两版一致）；交付物首切 `windos\dist\`。
- WP-K 启动页实例内版本选择（**commit 2ae6221**）：实例浮层展开版本子行，选中即按该版本启动。compact 5,706,240 B（**余量 1,024 B**）。
- WP-N premium 完整化：用户两次叫停，**停置未完成**（Fabric API 版本号/路径提示/加载器卡视觉/模组行增强）——留待后续。
- WP-J/WP-L（视觉对照报告、交互 E2E）：按用户指示撤下（"写文档的换下来"）。磁盘产物：`_wph_run\shots\` 20 张矩阵 + `shell\` 真窗截图，可供后续实修对照。
- 进行中（实修，2026-11-07）：WP-T = AI 入口改右侧玻璃药丸钮「✳ AI」+Ctrl+K（D-16）；WP-U = 启动页贴 launch2 实修（按钮内嵌版本/▶/联机图标）；WP-V = 实例库/下载/插件/设置四页贴图实修。三者文件不重叠。
- 额度监控按用户 2026-11-07 指示**停用**；子代理并发上限 2 → 后放宽（实测 3 并行可行）。
- 构建标准参数（D-14）：`dotnet build-server shutdown && dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1`（premium 加 `-p:Premium=true`）。
- 待办：WP-T/U/V 验收 → 全部落定后重切 dist（D-18）→ 总结报告。

## 2026-11-07 第四轮（WP-S2 记录）
本轮 10 个工作包实修并验收（详见交接必读.md §5）：

| 包 | 内容 | commit | 状态 |
|---|---|---|---|
| WP-F | 半成品审查+补完（删抽屉/死代码、字形、页签点亮、账号刷新） | 6ebc26e | 已验收 |
| WP-G | premium 版（PREMIUM 开关+嵌入图标/hero+毛玻璃） | ceb7061 | 已验收 |
| WP-K | 启动页实例内版本选择 | 2ae6221 | 已验收 |
| WP-T | AI 入口右侧玻璃药丸钮 ✳AI + Ctrl+K（D-16） | ebacae5 | 已验收 |
| WP-U | 启动页贴 launch2（按钮内嵌版本/▶/联机图标） | 99aec85 | 已验收 |
| WP-V | 四页贴图实修（实例卡 v2/标题 21 ExtraBold/圆点） | 486071d | 已验收 |
| WP-W | 窗口按钮规格复核+Win10 Region 防闪烁 | b539c19 | 已验收 |
| WP-X | 安装页贴图实修（头部/加载器卡/底栏） | 853a296 | 已验收 |
| WP-Y | 设置页分组卡片贴图实修 | 97a4c8a | 已验收 |
| WP-Z | 插件页卡片细部贴图实修 | 4518a56 | 已验收 |
| WP-N | premium 完整化（Fabric API 版本号/路径提示/加载器卡视觉/模组行增强） | — | 进行中（以 git log 为准） |

额度监控按用户 2026-11-07 指示停用，本轮不再记录百分比。

### 下一轮接手顺序
WP-N 落定确认 → 终版 smoke 回归 → 重切 dist（D-18）→（可选真窗验收）。

## 2026-11-07 第四轮收口（终态）
- 实修 14 包全部验收：WP-F 6ebc26e / WP-G ceb7061 / WP-K 2ae6221 / WP-T ebacae5 / WP-U 99aec85 / WP-V 486071d / WP-W b539c19 / WP-X 853a296 / WP-N 4b3af82 / WP-Y 97a4c8a / WP-Z 4518a56 / WP-S1 28b640e（build-both.ps1）/ WP-S2 0c326d6（第四轮文档+打包预演）/ WP-S3 bdf40bb（可达性调研）。
- 收口：R1 终版 smoke 回归全绿 f05e79a；dist 终版重切 f8e0a06（progress/dist-final.md 有哈希）。
- **交付物**：dist\TipCL-compact.exe 5,707,264 B（余量 50,000）/ TipCL-premium.exe 6,003,712 B。
- 待拍板遗留：WP-S4 中清理（12 死入口+Ctrl 数字键复活路径+WarmUp，约 120-165 行，需同步 NavCompatTests）；深清理 500+ 行留体积回收弹药；build-both.ps1 -Dist 覆盖 bug；真窗验收（Ctrl+K 实按/安装页实拍/主题切换实走）待用户放行。

## 2026-11-07 第五轮追加（实例库重做 + 隔离恢复 + 清理）
- WP-S5 77bdb0e（build-both 覆盖修复）/ WP-S4 d89d142（12 死入口清理，-2048B）/ WP-S6 198e00a（实例库双栏重做贴图2）/ WP-S4续 a7c08d1（版本管理页恢复隔离开关+大锅饭标签，根因见 WP-S7 调查：S4 删 version NavDef 致 VersionPage 孤儿化）。
- **终版交付物（dist）**：TipCL-compact.exe **5,729,280 B** SHA256 9568debf…2b78；TipCL-premium.exe **6,026,240 B** SHA256 6e668d43…2f29f。两哈希不同（脚本修复生效）。
- 终验：dotnet test 127/128（PhoneLink 既有）；dist compact --smoke 全绿（27/27 页 ok，0 异常；coverage 提示因 S4 删页后 17 点击/9 弹窗低于旧阈值 20/10——页数从 22 减到 10 所致，非回归，阈值需按新 IA 调整）。
- 遗留：真窗验收待用户；深清理（VersionPage 392 行）留体积回收。

## 2026-11-07 第六轮收口（反馈修复全落定）
- WP-S8b 27045cf：T1 头部两行+左栏 300~380 自适应（修正参数序 bug）；T2 详情页版本下拉+isolated=false 隐藏资源分段（含 DevShot --detail/--forceshared）。
- 全部已推送 GitHub main（9f56aaf→27045cf，远端核对一致；分支由 master 改名 main）。
- 终版交付物（dist）：TipCL-compact-20261107-1308-头部共享修复.exe 5,741,568 B SHA 580a71fb…；TipCL-premium-20261107-1308-头部共享修复.exe 6,038,528 B SHA e23ad3f4…。compact 余量 15,696 B。
- 终验：128/129（PhoneLink 环境性）；dist compact --smoke 27/27 页全 ok、0 异常（coverage 17/9 低于旧阈值 20/10 为 S4 删页后页数变化所致，非回归）。
- 待拍板：InstanceDetailPage 无真实 UI 入口（「管理」走对话框 vs 详情页）；PhoneLink 环境复跑；真窗验收。

## 2026-11-08 第七轮（UI 质量评价 → 全量修复）
- 起因：用户要求评价 UI 质量。实拍 12 张明暗截图 + 代码审查 + test/smoke，给出 7.5–8/10 评价与问题清单；用户指示「把你找出来的全部修复」。
- 修复清单（决策见 DECISIONS D-20…D-24）：
  | 项 | 内容 | 验证 |
  |---|---|---|
  | 窗口最小尺寸 | WndProc 改读 XAML Min（720×480），消除三处矛盾 | build |
  | 设置 hint 断行 | 三条 hint 缩短单行 + 词表成对更新 | 实拍明暗 |
  | 可访问性 | 5 模板焦点环、页签/标题栏 Tab 可达、图标按钮自动化名 | build/smoke |
  | 对比度 | 明暗 InkMuted/InkFaint 成对调整，全档 ≥4.5:1 | 脚本实测 + 实拍 |
  | 规范 | design-spec.md 重写 v2.0；圆角收敛 {8,12,999}+微元素豁免 | 实拍 |
  | 警告 | 1169→0（Nullable annotations / `_ =` / 删未用变量 / xUnit1012） | build 0 警告 |
  | ImageSharp | 5 条 CVE 不可达分析 + NuGetAuditSuppress + 4 个守卫测试 | test（含真解码） |
  | i18n | I18nCheck 路径修复 + 词表回退 + 补 254 缺失键（en 人工翻译） | missing_en/zh=0 |
  | DevShot | consent 弹窗确定性点掉（暂不同意），截图不再被污染 | 12 张干净截图 |
  | 巨型文件 | 3 个 partial 提取（Themes/Popups/Images），零逻辑改动 | build/test/smoke |
- 终验：build 0 错误 0 警告；test 157/158（唯一失败=基线 PhoneLink）；smoke 10/10 页 ok、17 点击、10 弹窗、0 异常；i18n missing=0；明暗 12 图视觉回归通过。
- 产物：截图 `windos/_eval_shots/final/`；smoke 报告 `_eval_smoke_home/_wpf_smoke/run-20261108-153018/`；分析 `docs/security/imagesharp-audit.md`。
- 遗留（记录不修，属另一轮）：① unwrapped 中文字面量 2897（Core 2555 / Pages 193，多为 AI 提示词与自检串，需逐条判断）；② 体积超 D-19 红线（镜像提交先超 ~370KB + 本轮词表 ~110KB），dist 重切与回收待排期；③ PhoneLink 基线失败（Core 业务，D-4 在案）。

## 2026-11-14 第八轮(布局 v2:「高度自定义布局」全链路)

用户 2026-11-14 需求:给 TipCL 增加自定义程度更高的自定义布局;范围 A–G、全部页面统一,硬约束 = **布局 JSON 必须向后兼容(不丢用户已有布局)**。设计与契约冻结在 `docs/layout-v2-design.md`(含 §0.1 决策记录),本轮决策见 DECISIONS **D-25…D-33**。构建一律 D-14 全参数;测试一律先把 `TEMP/TMP` 指到 `D:\项目\tipcl\_tmp`(否则 40 个用例因建不了临时目录假失败)。**本轮全部改动尚未 commit**;git 提交由监工统一做。

### 工作包总览

| 包 | 内容 | 关键文件(独占) | 验证(证据在树里) | 状态 |
|---|---|---|---|---|
| L0 | 契约骨架:v2 模型 / 校验 / 存储 + v1 双向兼容 | `Pages/Layout/LayoutDocV2.cs`、`LayoutSchema.cs`、`LayoutStoreV2.cs`、`LayoutJson.cs`;`tests/LayoutV2ModelTests.cs`、`LayoutV2CompatTests.cs` | 新增 35 例(Model 24 + Compat 11)全绿;`LayoutDefaultTests`/`PageLayoutTests`/`NavCompatTests` 保持全绿 | 完成 |
| L1 | 引擎 + BlockHost 接入 v2(stack 等价迁移 + `span` 跨列 + 内容工厂钩子) | `Pages/Layout/LayoutEngine.cs`、`LayoutEditor.cs`;`Pages/BlockHost.cs`;`tests/LayoutEngineTests.cs`、`LayoutBlockHostTests.cs` | 6 站点改动前后截图:`artifacts/l1/before/`、`artifacts/l1/after/`、`artifacts/l1/after-final/`(`artifacts/l1/README.md`;改动前 exe 备份 `artifacts/l1/exe/TipCL-before.exe`) | 完成 |
| L2 | grid / canvas 三模式 + 手柄 / 吸附 / 对齐辅助线 + 撤销重做 / 多选 / 键盘 + 外观参数(style / title / pad / density)+ 响应式 variants | `Pages/Layout/GridCanvasPanel.cs`、`GuideLayer.cs`、`LayoutEditor.cs`、`LayoutEngine.cs`;`tests/LayoutCanvasTests.cs`、`LayoutEditorTests.cs` | `artifacts/l2/after/`(含 `edit/` 编辑态)、`artifacts/l2/variants/`(900 / 800 / 700 宽对照)、`artifacts/l2/imgdiff.py` | 完成(期间 5 个失败用例已由本包修复) |
| L4 | 分栏比例持久化(13 处)+ `PageLayoutHost` + 双击复位 | `Pages/Layout/PageLayoutHost.cs` + LibraryPage / SettingsPage / InstanceDetailPage / DownloadPage / CatalogPage / InstallPage / VersionManagePage / AiPage / FeedbackPage / AccountPage / PluginsPage;`tests/LayoutSplitTests.cs` | 默认比例 = 现状的逐页截图:`artifacts/shot-{library,instance,account,ai,plugins,settings}-dark.png`;拖后 / 复位 `artifacts/shot-*-ratio-dark.png`、`-reset-dark.png` | 完成 |
| L5 | 设置页「布局」视图(7 小节)+ 整机方案增删改 / 导入导出 | `Pages/SettingsLayoutPage.cs`;`tests/LayoutProfileTests.cs` | `artifacts/shot-settings-layout-{dark,tall-dark,clean-dark,full-dark}.png`;方案与导入导出 14 例 | 完成 |
| L6 | 自建区块(note / shortcut / clock / divider / playtime)+ 危险类型默认关闭 + 插件 kind `block` + AI 布局工具 | `Pages/Layout/WidgetRegistry.cs`、`Pages/Layout/Widgets/*.cs`(7 个);`Core/Plugins/{PluginManifest,PluginHostApi,PluginManager}.cs`;`Core/Launcher/Ai/LayoutTools.cs`;`tests/LayoutWidgetTests.cs` | `artifacts/l6/widget-fixture.ui_layout_v2.json`、`artifacts/l6/tasks-widgets*.png`、`artifacts/l6/README.md`;44 例 | 完成 |
| L7a | 顶栏页签数据驱动(`shell.tabs`)+ 标题栏元素开关(`shell.titlebar.*`) | `Pages/Layout/ShellTabs.cs`;`MainWindow.xaml`、`MainWindow.xaml.cs`;`tests/LayoutShellTests.cs` | 默认等价 = 改动前后 PNG **SHA256 相同**;27 例;证据 `artifacts/shot-tabs-dark.png`、`artifacts/shot-tabs-dark-topdiff.png`、`artifacts/l7a_topstrip_diff.py` | 完成(**冒烟未跑通**,见遗留) |
| D1 | 接线包:MainWindow / LayoutEditor / LayoutEngine / BlockHost 收口 | 同上若干文件 | `artifacts/l2/after/d1-*.png` | **未落定 —— 待追加** |
| D4 | 插件端到端:插件区块在真实插件里的落地验证 | 待定(`samples/hello-plugin` 侧) | — | **未落定 —— 待追加** |
| D2 | 词表成对补齐(审计驱动)+ 工程记录(本包) | `Resources/locales/en.json`、`zh_CN.json`;`PROGRESS.md`;`DECISIONS.md` | 见「D2 实测」 | 完成 |

### L0 · 契约骨架 + v1 双向兼容

- **做了什么**:新增 v2 文档模型(信封 / `shell` / `pages` / `splits` / `widgets` / `variants` / `profiles`)、唯一契约真相 `LayoutSchema`(白名单 + 钳位表 + `Sanitize`)、`LayoutStoreV2`(读写 `ui_layout_v2.json`、`FromV1` 迁移、**由 v2 单向写通 v1**、`DirOverride` 测试注入、`LayoutChanged` 事件)。读失败一律回落 v1 子集再回落默认,**绝不抛、绝不清空用户数据**。
- **关键文件**:`src/TipCL/Pages/Layout/LayoutDocV2.cs`、`LayoutSchema.cs`、`LayoutStoreV2.cs`、`LayoutJson.cs`;`tests/TipCL.Tests/LayoutV2ModelTests.cs`、`LayoutV2CompatTests.cs`。
- **验证**:新增 35 例全绿(Model 24 / Compat 11),覆盖契约逐字段默认值/钳位、迁移逐项保留、写通后用**冻结的 v1 解析器**读回全等、旧版重写 v1 后 v2 新字段不丢、损坏回落、导入校验;本轮收口时这两个文件仍是 35 例全绿。
- **遗留/风险**:v2 与 v1 双写分叉风险由「v2 唯一真源 + 单向写通」压住;`LayoutSchema.NavKeys` / `DefaultNavOrder` 是刻意复制的字面量(避开 NavModel 的静态初始化),契约若变需要人工对齐(设计文档已标注)。

### L1 · 引擎 + BlockHost 接入 v2

- **做了什么**:抽出 `LayoutEngine`(stack 模式的几何/排布/命中,支持 `blocks[].span` 跨列)与 `LayoutEditor`(编辑态框架:拖拽、显隐、托盘、工具条、提交时 `LayoutStoreV2.Save`);`BlockHost` 内部改为「创建引擎 + 选模式」,**公共契约 `BlockSpec` / `BlockHost(pageId,specs,margin)` / `Root` / `Changed` 一字不改**(12 个站点零改动);给自建区块/插件块留下内容工厂钩子(`custom:` / `plugin:` 查不到显示「不可用」)。
- **关键文件**:`src/TipCL/Pages/Layout/LayoutEngine.cs`、`LayoutEditor.cs`;`src/TipCL/Pages/BlockHost.cs`;`tests/TipCL.Tests/LayoutEngineTests.cs`、`LayoutBlockHostTests.cs`。
- **验证**:`dotnet build`(D-14 全参数)+ 逐站点 `--shot` 改动前后对照,产物 `artifacts/l1/before|after|after-final/`(含 `launch/tasks/settings/account/download/multiplayer`,另有编辑态 `edit-*.png` 与 `control/edit-*.png`);`LayoutEngineTests` 18 + `LayoutBlockHostTests` 18 例全绿。
- **遗留/风险**:默认布局的「逐像素等价」在老代码里只能人眼比,本包为此保留了改动前 exe 与截图(D-26 起改为 SHA256 机器判等);`PageLayoutStore` 的进程内缓存由 L1 加了 `Reload()`,写通后旧路径才立刻看到新值。

### L2 · 三模式画布 + 外观参数 + 编辑体验

- **做了什么**:`grid`(12 列可吸附列数,跨列跨行、行高自适应)/ `canvas`(归一化矩形 + 8 向手柄 + 栅格吸附)两种新模式与 `StackMode` 并存;新增 `GridCanvasPanel`(自绘排布)+ `GuideLayer`(对齐辅助线);编辑器补多选 / 框选 / 撤销重做 / 键盘(`Ctrl+Z/Y`、方向键微调、`Esc`、`Delete`)/ 复制粘贴;外观参数 `style`(plain / card / divider)、`title`、`pad`、全局 `density`;响应式 `variants`(跨窗口宽度阈值切换)。
- **关键文件**:`src/TipCL/Pages/Layout/GridCanvasPanel.cs`、`GuideLayer.cs`、`LayoutEditor.cs`、`LayoutEngine.cs`;`tests/TipCL.Tests/LayoutCanvasTests.cs`、`LayoutEditorTests.cs`。
- **验证**:`--shot` 逐页 + 编辑态截图(`artifacts/l2/after/`、`artifacts/l2/after/edit/`);响应式三档宽度对照 `artifacts/l2/variants/`(900 / 800 / 700 px,`imgdiff.py` 判等);`LayoutCanvasTests` 41 + `LayoutEditorTests` 23 例全绿。收口前本包曾有 5 个失败用例,已由本包修完(本轮收口 0 失败)。
- **遗留/风险**:块高度不定靠「行高自适应两趟测量」解,`canvas` 只对声明了 `row_h` 或固定高的块开放;编辑态才接管滚动手势,非编辑态行为不变(设计 §4.A 的硬障碍表逐条对应)。

### L4 · 分栏比例持久化(13 处)

- **做了什么**:新增 `PageLayoutHost`(在每处两栏之间插 `GridSplitter`、比例读写 `splits[pageId][splitId]`、双击复位、保留各页既有 Min/MaxWidth),逐页接入 13 处;内嵌态(版本管理页嵌在详情页时)隐藏分隔条且不写比例;默认比例 = 现状硬编码值。
- **关键文件**:`src/TipCL/Pages/Layout/PageLayoutHost.cs`;`src/TipCL/Pages/{LibraryPage,SettingsPage,InstanceDetailPage,DownloadPage,CatalogPage,InstallPage,VersionManagePage,AiPage,FeedbackPage,AccountPage,PluginsPage}.cs`;`tests/TipCL.Tests/LayoutSplitTests.cs`。
- **验证**:逐页 `--shot` 对照(默认态与改动前一致)、拖后与复位各一张:`artifacts/shot-{library,instance,account,ai,plugins,settings}-dark.png`、`artifacts/shot-{library,account,ai}-ratio-dark.png`、`artifacts/shot-{library,account,ai}-reset-dark.png`;`LayoutSplitTests` 16 例全绿。
- **遗留/风险**:`SettingsPage.cs` 是 100KB 大文件且有 `StaticInitOrderTests` 守卫,只做「左导航分栏」这一件事,新「布局」视图由 L5 承担;比例夹 `0.15..0.85`,细则见设计 §4.E。

### L5 · 设置页「布局」视图 + 整机方案

- **做了什么**:设置页新增第 8 个视图「布局」(外观与密度 / 页签与标题栏 / 页面区块 / 分栏比例 / 自建区块 / 方案 / JSON 编辑器),含整机方案的新建 / 应用 / 重命名 / 删除 / 复制 / 导出(整机 v2 或兼容 v1 的页面布局)/ 导入;JSON 编辑器按字段给中文校验原因,校验通过才落盘。
- **关键文件**:`src/TipCL/Pages/SettingsLayoutPage.cs`;`tests/TipCL.Tests/LayoutProfileTests.cs`。
- **验证**:`artifacts/shot-settings-layout-dark.png`、`-tall-dark.png`、`-clean-dark.png`、`-full-dark.png`;`LayoutProfileTests` 14 例全绿(方案的增删改、应用=原子替换、导入导出校验)。
- **遗留/风险**:单块页(下载与网络 / 反馈与关于)的编辑入口由后续包补(设计 §4.C 的 R9);本包自报新增 108 条中文串,**以 D2 的独立审计差集为准**(布局相关文件合计缺 178 条)。

### L6 · 自建区块 + 插件 kind `block` + AI 工具

- **做了什么**:`WidgetRegistry` + 5 种首期自建区块(note / shortcut / clock / divider / playtime)与危险类型(web / button);插件 UI 贡献新增 kind `block`(块 id = `plugin:<plugin>:<blockId>`),插件卸载后保留并显示「不可用」;AI 侧注册布局工具(读/写带确认元数据)。
- **关键文件**:`src/TipCL/Pages/Layout/WidgetRegistry.cs`、`Pages/Layout/Widgets/{BasicWidgets,ShortcutWidget,PlaytimeWidget,DangerousWidgets,PluginBlocks,WidgetCommon,WidgetUI}.cs`;`src/TipCL/Core/Plugins/PluginManifest.cs:77`、`PluginHostApi.cs:232`、`PluginManager.cs:1259`(三处白名单同值 `page / aiView / dashCard / block / overlay / settings / action`);`src/TipCL/Core/Launcher/Ai/LayoutTools.cs`;`tests/TipCL.Tests/LayoutWidgetTests.cs`。
- **验证**:插件区块夹具与危险类型截图 `artifacts/l6/widget-fixture.ui_layout_v2.json`、`artifacts/l6/tasks-widgets-dark.png`、`artifacts/l6/tasks-widgets-dangerous-dark.png`、`artifacts/l6/README.md`;`LayoutWidgetTests` 44 例全绿(含危险类型默认关闭)。
- **遗留/风险**:设计 §5.1 里列的 `Core/Launcher/LayoutV2Core.cs` 与 `Core/BackendApi.Layout.cs` **在树里没有对应文件**(RPC / AI 工具走的是 `Core/Launcher/Ai/LayoutTools.cs`),若后续要对外开放布局 RPC 需要补;`web` 禁止 `file://`、`button` 只允许内部动作白名单(`page / launch / folder / rpc`)见 D-30。

### L7a · 顶栏页签 + 标题栏元素开关(本成员交付)

- **做了什么**:页签文案与顺序从 XAML 写死改为 `shell.tabs` 数据驱动。`ShellTabs.cs` 是纯逻辑层(禁 WPF):出「顺序 + 显隐」计划(白名单 / 去重 / 空回落 `LayoutSchema.DefaultTabs`)、`Label()` 显式走 `I18n.L`、`HighlightKeyOf()` 逐项等价改写 `SyncTabs` 五行;`MainWindow.RebuildShellTabs` 重建 `TopTabPanel`(隐藏的不进可视树),插件动态页签仍由 `MainWindow.Plugins.cs` 追加在末尾;`shell.titlebar.*` 只改 `BridgePill / AiBtn / ThemeBtn / AccBtn` 的 `Visibility`(AI 入口形态 D-16 与窗口按钮一律不动);写回只走 `ShellTabs.SetTabs / SetTabVisible / SetTitlebar` → `LayoutStoreV2.Save`。
- **关键文件**:`src/TipCL/Pages/Layout/ShellTabs.cs`;`src/TipCL/MainWindow.xaml`、`MainWindow.xaml.cs`;`tests/TipCL.Tests/LayoutShellTests.cs`。
- **验证**:命令 `dotnet build`(D-14 全参数)→ `TipCL.exe --shot launch --out artifacts\shot-tabs-dark.png --theme dark`,与**改动前 exe**(`artifacts/l1/exe/TipCL-before.exe`,字节搜索确认不含 `_shellTabMap` / `RebuildShellTabs`)产出的 `artifacts/l1/before/launch-dark.png` 对比:两张 **SHA256 同为 `D212F4287AA08FDB5F146CA6FFC05358F8F2B0B9304E12AB1F4E1256CE8B97FD`**、900×550、**整图 0 像素差异**(`artifacts/l7a_topstrip_diff.py` + Pillow;顶栏 46px 带 41,400 像素 0 差异,`artifacts/shot-tabs-dark-topdiff.png` 全黑)。`LayoutShellTests` 27 例全绿(默认五项、顺序显隐、13 条高亮映射、文案成对、v1 冻结、XAML 无中文字面量)。
- **遗留/风险**:1 **冒烟未跑通**(见「本会话环境事实」);2 插件动态页签末尾追加只有代码推理(当前数据目录无 top 组插件页,截不到);3 隐藏 `BridgePill` 时「TipCL」字标自带 8px 左边距保留;4 页签全隐藏时 `Sanitize` 回落默认五项(「界面永远有入口」);5 设置页里的页签/标题栏复选框 UI 属 L5。

### D1 · 接线包(待追加)

设计里的 D1 = 把 L1/L2/L7a 的外壳与编辑链路口径统一收口(MainWindow / LayoutEditor / LayoutEngine / BlockHost)。**截至本轮记录时尚未落定**,只留下中间取证 `artifacts/l2/after/d1-*.png`。落定后在本节补:改动清单、逐页 `--shot` 对照、最终构建与测试数字。

### D4 · 插件端到端(待追加)

D4 = 插件区块在真实插件里的端到端验证(用 `samples/hello-plugin` 之类走一遍:装载 → 渲染 → 卸载后显示「不可用」)。**截至本轮记录时尚未落定**,本节待追加。

### D2 · 词表成对补齐 + 工程记录(本包)

- **审计方法(不采信转述条数)**:脚本扫 `Pages/Layout/**`(含 `Widgets/`)、`BlockHost.cs`、`SettingsLayoutPage.cs`、`SettingsPage.cs`、`MainWindow.xaml.cs`、`ShellTabs.cs`、`Core/Launcher/Ai/LayoutTools.cs`、`Core/Plugins/**` 里全部 `I18n.L(...)` / `I18nCore.Tr(...)` 的中文字面量,与 `en.json` + `zh_CN.json` 求差集。结果 = **557 条去重 key,178 条两边都缺**(转述的 108 / 11 / 1 条加起来对不上)。
- **补齐**:`en.json` 2774 → **2952**(+178,人工英文);`zh_CN.json` 2773 → **2952**(+179,含 HEAD 就缺的 `自定义布局` 补恒等映射)。逐行文本插入在文件顶部(沿用文件里「新键在前」的既有做法),**既有行一字未动**。
- **复核**:JSON 合法;两文件**键集合完全一致**;**既有键的值 0 处改动**、既有行逐行原样保留;新增 zh 条目全部自映射;新增 en 条目无空值、无直接抄中文;**差集归零(en 缺 0 / zh 缺 0)**;UTF-8 无 BOM、LF、末尾换行不变。落盘后字节:en 196,270 B / zh 188,967 B。
- **顺带发现(记录不修)**:1 两处**既有近似重复键**,源码字面量与词表既有 key 只差标点宽度 —— `已导入 {0} 个页面的布局(下次打开对应页面生效)`(`(` U+0028 vs 词表 `（` U+FF08)与 `布局已应用,可直接「另存为方案」保留`(`,` U+002C vs 词表 `，` U+FF0C),会让这两条在 en 下查不到译文,本包按源码原样补了新 key;2 两个文件**既有**键顺序自第 365 键起就不一致(集合一致、顺序分叉),本包未动既有顺序。

### 本会话环境事实(均已复现,留给后续轮次)

- **`--smoke` 在本会话环境跑不动**:两次(`--smoke-timeout 600` 与 180)都卡在 `Smoke.cs:273` 要新建的报告目录 `<数据目录>\_wpf_smoke\<run>`(工作区外)—— 进程约 0.6s CPU 后长期空闲、`_wpf_smoke\run-20261114*` 在仓库 / LocalAppData / Roaming 全都不存在、`Watchdog` 因此从未启动(它在建目录之后才 arm)。这与 **DECISIONS §183 记的进程级「工作区外不能建目录」限制**同类(`%TEMP%` 那次的表现一致)。**未验证的推测解**:把 `TIPCL_HOME` 指到工作区内目录(如 `windos\_l7a_smoke_home`)再跑 —— 2026-11-08 那批跑通的冒烟用的正是 `TIPCL_HOME=D:\项目\tipcl\windos\_eval_smoke_home`。
- **`--i18n-check` 在本会话环境也跑不起来**:不设 `TIPCL_HOME` → `没有源码树: ...\wpf\TipCL`(exit 2);设 `TIPCL_HOME=仓库根` → 进程零输出且不退出(疑似走提权重启路径)。替代做法 = D-33 的差集审计脚本。
- **构建位互锁(D-14)**:构建前必须 `Get-Process dotnet` **并**确认没有 `TipCL.exe` 在跑 —— 本轮实测过一次 `MSB3021/MSB3027`:另一位成员的 GUI 进程锁住 `bin\Release\net48\TipCL.exe`,输出的 exe 复制失败(编译本身 0 错 0 警,exe 不变)。

### 收口实测数字(2026-11-14,D2 包)

- 构建:`dotnet build TipCL.sln -c Release -p:UseSharedCompilation=false -p:DebugType=none -p:DebugSymbols=false -m:1` → **0 错误 0 警告**;`src\TipCL\bin\Release\net48\TipCL.exe` = **6,439,936 B**(含本包词表增补;超 D-19 红线按 D-28 豁免并如实记录)。
- 测试:`dotnet test TipCL.sln -c Release --no-build --filter "FullyQualifiedName!~PhoneLinkTests"` → **412/412 通过、0 失败**(监工复核口径为 349 → **407** 全绿;此后 D1 又补了用例,故收口为 412 —— 口径一致,无回归)。
- 布局相关用例(按 `--list-tests` 枚举,共 **236** 例):LayoutV2ModelTests 24 / LayoutV2CompatTests 11 / LayoutEngineTests 18 / LayoutBlockHostTests 18 / LayoutEditorTests 23 / LayoutCanvasTests 41 / LayoutSplitTests 16 / LayoutProfileTests 14 / LayoutWidgetTests 44 / LayoutShellTests 27。

### 待办(下一轮)

1. **D1 接线包**与 **D4 插件端到端**落定后各补一节(改动清单 + 逐页 `--shot` + 最终数字)。
2. 冒烟:按「环境事实」条把 `TIPCL_HOME` 指到工作区内目录后补跑 `--smoke --smoke-timeout 600`,把报告路径补进本轮。
3. 体积:6,439,936 B 已超红线(豁免见 D-28);若要回收,候选见 `docs/size-report.md` 与 D-24 的体积提示。
4. 两处既有近似重复键(标点宽度)由维护者决定归并方式(改 .cs 字面量或改词表 key,二者影响面不同)。
5. 移植 mac/linux 前先还 D-32 记的依赖方向债务(4 个纯模型文件移入 `Core/Launcher/Layout/`)。
