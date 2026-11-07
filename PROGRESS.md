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
